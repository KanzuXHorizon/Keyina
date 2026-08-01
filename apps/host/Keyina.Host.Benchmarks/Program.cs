using System.Runtime.InteropServices;

namespace Keyina.Host.Benchmarks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var suite = "snippets";
        var output = Path.Combine("artifacts", "benchmarks", "managed");
        var warmup = 100;
        var iterations = 1_000;
        string? residentExecutable = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--suite" when index + 1 < args.Length:
                    suite = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--resident" when index + 1 < args.Length:
                    residentExecutable = args[++index];
                    break;
                case "--warmup" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedWarmup) && parsedWarmup > 0:
                    warmup = parsedWarmup;
                    index++;
                    break;
                case "--iterations" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedIterations) && parsedIterations > 0:
                    iterations = parsedIterations;
                    index++;
                    break;
                default:
                    PrintUsage();
                    return 2;
            }
        }

        try
        {
            var cases = RunSuitesAsync(suite, residentExecutable, warmup, iterations)
                .GetAwaiter()
                .GetResult();
            var document = new BenchmarkDocument(
                1,
                new BenchmarkEnvironment(
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    Environment.ProcessorCount,
                    warmup,
                    iterations),
                cases);
            BenchmarkReport.Write(output, document);

            foreach (var item in cases)
            {
                Console.WriteLine(
                    $"{item.Name}: median={item.MedianNanoseconds:F1}ns p95={item.P95Nanoseconds:F1}ns alloc={item.AllocatedBytesPerOperation:F1}B/op");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<List<BenchmarkCase>> RunSuitesAsync(
        string suite,
        string? residentExecutable,
        int warmup,
        int iterations)
    {
        var requested = suite.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requested.Length == 0)
        {
            throw new ArgumentException("At least one benchmark suite is required.");
        }

        var cases = new List<BenchmarkCase>();
        foreach (var item in requested)
        {
            switch (item.ToLowerInvariant())
            {
                case "all":
                    cases.AddRange(SnippetBenchmarks.Run(warmup, iterations));
                    cases.AddRange(CommandOutputBenchmarks.Run(Math.Min(warmup, 5), Math.Min(iterations, 20)));
                    cases.AddRange(await ApplicationBenchmarks.RunAsync(warmup, iterations)
                        .ConfigureAwait(false));
                    if (!string.IsNullOrWhiteSpace(residentExecutable))
                    {
                        cases.AddRange(ResidentBenchmarks.Run(residentExecutable, warmup, iterations));
                    }
                    break;
                case "snippets":
                    cases.AddRange(SnippetBenchmarks.Run(warmup, iterations));
                    break;
                case "commands":
                    cases.AddRange(CommandOutputBenchmarks.Run(Math.Min(warmup, 5), Math.Min(iterations, 20)));
                    break;
                case "application":
                    cases.AddRange(await ApplicationBenchmarks.RunAsync(warmup, iterations)
                        .ConfigureAwait(false));
                    break;
                case "settings":
                    cases.AddRange(ApplicationBenchmarks.RunSnippetUi(warmup, iterations));
                    break;
                case "resident" when !string.IsNullOrWhiteSpace(residentExecutable):
                    cases.AddRange(ResidentBenchmarks.Run(residentExecutable, warmup, iterations));
                    break;
                case "resident":
                    throw new ArgumentException("The resident suite requires --resident <KeyinaInput.exe>.");
                default:
                    throw new ArgumentException($"Unsupported benchmark suite: {item}");
            }
        }
        return cases;
    }

    private static void PrintUsage() => Console.Error.WriteLine(
        "Usage: Keyina.Host.Benchmarks --suite snippets|commands|application|settings|resident|all[,suite] " +
        "--output <directory> [--resident <KeyinaInput.exe>] [--warmup N] [--iterations N]");
}
