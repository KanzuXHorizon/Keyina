namespace Keyina.Host.Core.Snippets;

public sealed class SnippetMatcher
{
    private readonly Dictionary<string, Entry> _caseSensitive =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _caseInsensitive =
        new(StringComparer.OrdinalIgnoreCase);

    public SnippetMatcher(IEnumerable<SnippetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var unambiguousTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!unambiguousTriggers.Add(definition.Trigger))
            {
                throw new ArgumentException(
                    $"Duplicate or case-ambiguous snippet trigger: {definition.Trigger}.",
                    nameof(definitions));
            }

            var entry = new Entry(definition, definition.Validate());
            var index = definition.CaseSensitive ? _caseSensitive : _caseInsensitive;
            index.Add(definition.Trigger, entry);
        }
    }

    public SnippetMatch? Match(
        ReadOnlySpan<char> token,
        char delimiter,
        in SnippetContext context)
    {
        if (context.SecureInput || token.IsEmpty)
        {
            return null;
        }

        var tokenText = token.ToString();
        if (!_caseSensitive.TryGetValue(tokenText, out var entry) &&
            !_caseInsensitive.TryGetValue(tokenText, out entry))
        {
            return null;
        }

        var definition = entry.Definition;
        if (!definition.Delimiters.Contains(delimiter))
        {
            return null;
        }

        var applicationId = context.NormalizedApplicationId;
        if (definition.ExcludedApplications.ContainsOrdinalIgnoreCase(applicationId))
        {
            return null;
        }
        if (definition.AllowedApplications.Count > 0 &&
            !definition.AllowedApplications.ContainsOrdinalIgnoreCase(applicationId))
        {
            return null;
        }

        return new SnippetMatch(
            entry.TriggerCodePoints,
            definition.Command == SnippetCommand.None
                ? SnippetVariableExpander.Expand(definition.Expansion, context.Now)
                : string.Empty,
            definition.PreserveDelimiter,
            definition.Command);
    }

    private sealed record Entry(SnippetDefinition Definition, int TriggerCodePoints);
}

internal static class SnippetSetExtensions
{
    public static bool ContainsOrdinalIgnoreCase(
        this IReadOnlySet<string> values,
        string candidate)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public static class BuiltInSnippets
{
    private static readonly IReadOnlySet<char> DefaultDelimiters =
        new HashSet<char> { ' ', '\t', '\r', '\n' };
    private static readonly IReadOnlySet<string> NoApplications =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SnippetDefinition> Create() =>
    [
        Command(";kvi", SnippetCommand.ToggleVietnamese),
        Command(";kvoice", SnippetCommand.ToggleDictation),
        Text(";kdate", "${date}"),
        Text(";ktime", "${time}"),
        Text(";kdatetime", "${datetime}"),
    ];

    private static SnippetDefinition Command(string trigger, SnippetCommand command) =>
        new(
            trigger,
            string.Empty,
            CaseSensitive: true,
            PreserveDelimiter: false,
            DefaultDelimiters,
            NoApplications,
            NoApplications,
            command);

    private static SnippetDefinition Text(string trigger, string expansion) =>
        new(
            trigger,
            expansion,
            CaseSensitive: true,
            PreserveDelimiter: true,
            DefaultDelimiters,
            NoApplications,
            NoApplications,
            SnippetCommand.None);
}
