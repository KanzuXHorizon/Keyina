namespace Keyina.Host.Core.Snippets;

public sealed class SnippetSuggestionSession
{
    private readonly List<SnippetDefinition> definitions = [];
    private string prefix = string.Empty;

    public string Prefix => prefix;

    public void UpdateDefinitions(IEnumerable<SnippetDefinition> updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        definitions.Clear();
        definitions.AddRange(updated.OrderBy(item => item.Trigger, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<SnippetDefinition> Push(char character)
    {
        if (character == '\b')
        {
            prefix = prefix.Length == 0 ? string.Empty : prefix[..^1];
        }
        else if (character == '\u001b' || char.IsWhiteSpace(character))
        {
            prefix = string.Empty;
        }
        else if (prefix.Length == 0)
        {
            prefix = character == ';' ? ";" : string.Empty;
        }
        else
        {
            prefix += char.ToLowerInvariant(character);
        }

        if (!prefix.StartsWith(";k", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(prefix, ";", StringComparison.Ordinal))
            {
                prefix = string.Empty;
            }
            return Array.Empty<SnippetDefinition>();
        }

        var start = FindFirstCandidate(prefix);
        if (start == definitions.Count)
        {
            return Array.Empty<SnippetDefinition>();
        }

        var matches = new List<SnippetDefinition>(8);
        for (var index = start; index < definitions.Count && matches.Count < 8; index++)
        {
            var definition = definitions[index];
            if (!definition.Trigger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            matches.Add(definition);
        }
        return matches.Count == 0 ? Array.Empty<SnippetDefinition>() : matches.ToArray();
    }

    public void Reset() => prefix = string.Empty;

    private int FindFirstCandidate(string value)
    {
        var low = 0;
        var high = definitions.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (StringComparer.OrdinalIgnoreCase.Compare(definitions[middle].Trigger, value) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }
}
