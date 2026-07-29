using System.Globalization;

namespace Keyina.Host.Core.Snippets;

public static class SnippetVariableExpander
{
    private static readonly string[] SupportedVariables =
    [
        "date",
        "time",
        "datetime",
    ];

    public static void Validate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var searchIndex = 0;
        while (true)
        {
            var start = template.IndexOf("${", searchIndex, StringComparison.Ordinal);
            if (start < 0)
            {
                return;
            }

            var end = template.IndexOf('}', start + 2);
            if (end < 0)
            {
                throw new ArgumentException("Snippet variable is missing a closing brace.", nameof(template));
            }

            var variable = template[(start + 2)..end];
            if (!SupportedVariables.Contains(variable, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unsupported snippet variable: {variable}.", nameof(template));
            }

            searchIndex = end + 1;
        }
    }

    public static string Expand(string template, DateTimeOffset now)
    {
        Validate(template);
        return template
            .Replace("${datetime}", now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("${date}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("${time}", now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
