using Keyina.Host.Core.Snippets;

namespace Keyina.Host.Benchmarks;

internal static class SnippetBenchmarks
{
    internal static IReadOnlyList<BenchmarkCase> Run(int warmupIterations, int measuredIterations)
    {
        var results = new List<BenchmarkCase>();
        foreach (var count in new[] { 10, 100, 1_000, 10_000 })
        {
            var definitions = CreateDefinitions(count);
            results.Add(MeasureSequence($"snippet_prefix_{count}", definitions, ";kitem0", warmupIterations, measuredIterations));
            results.Add(MeasureSequence($"snippet_miss_{count}", definitions, ";kmissing", warmupIterations, measuredIterations));
            results.Add(MeasureSequence($"snippet_unicode_{count}", definitions, ";ktiếng", warmupIterations, measuredIterations));
        }
        return results;
    }

    private static BenchmarkCase MeasureSequence(
        string name,
        IReadOnlyList<SnippetDefinition> definitions,
        string sequence,
        int warmupIterations,
        int measuredIterations)
    {
        var session = new SnippetSuggestionSession();
        session.UpdateDefinitions(definitions);
        return BenchmarkReport.Measure(name, warmupIterations, measuredIterations, () =>
        {
            session.Reset();
            IReadOnlyList<SnippetDefinition> matches = Array.Empty<SnippetDefinition>();
            foreach (var character in sequence)
            {
                matches = session.Push(character);
            }
            return matches.Count + session.Prefix.Length;
        });
    }

    private static List<SnippetDefinition> CreateDefinitions(int count)
    {
        var definitions = new List<SnippetDefinition>(count + 1);
        for (var index = 0; index < count; index++)
        {
            definitions.Add(new SnippetDefinition(
                $";kitem{index:D6}",
                $"Expansion {index}",
                false,
                false,
                new HashSet<char> { ' ', '\n' },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        }
        definitions.Add(new SnippetDefinition(
            ";ktiếngviệt",
            "Tiếng Việt",
            false,
            false,
            new HashSet<char> { ' ', '\n' },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        return definitions;
    }
}
