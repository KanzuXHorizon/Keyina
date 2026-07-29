using Keyina.Host.Core.Snippets;

namespace Keyina.Host.Core.Configuration;

public enum KeyinaTheme
{
    System,
    Light,
    Dark,
}

public sealed record SnippetConfiguration(
    string Trigger,
    string Expansion,
    bool CaseSensitive,
    bool PreserveDelimiter,
    string Delimiters,
    string[] AllowedApplications,
    string[] ExcludedApplications)
{
    public SnippetDefinition ToDefinition()
    {
        if (Delimiters is null)
        {
            throw new ArgumentException("Snippet delimiters must not be null.", nameof(Delimiters));
        }

        return new SnippetDefinition(
            Trigger,
            Expansion,
            CaseSensitive,
            PreserveDelimiter,
            Delimiters.ToHashSet(),
            (AllowedApplications ?? throw new ArgumentException(
                "Allowed application list must not be null.",
                nameof(AllowedApplications))).ToHashSet(StringComparer.OrdinalIgnoreCase),
            (ExcludedApplications ?? throw new ArgumentException(
                "Excluded application list must not be null.",
                nameof(ExcludedApplications))).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}

public sealed record KeyinaConfiguration(
    int SchemaVersion,
    bool VietnameseEnabled,
    bool SpeechEnabled,
    KeyinaTheme Theme,
    SnippetConfiguration[] Snippets)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumCustomSnippets = 10_000;

    public static KeyinaConfiguration Default { get; } = new(
        CurrentSchemaVersion,
        VietnameseEnabled: true,
        SpeechEnabled: false,
        KeyinaTheme.System,
        []);

    public IReadOnlyList<SnippetDefinition> ValidateAndCreateSnippets()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ConfigurationValidationException(
                $"Unsupported configuration schema version: {SchemaVersion}.");
        }
        if (!Enum.IsDefined(Theme))
        {
            throw new ConfigurationValidationException("Configuration theme is invalid.");
        }
        if (Snippets is null)
        {
            throw new ConfigurationValidationException("Snippet collection must not be null.");
        }
        if (Snippets.Length > MaximumCustomSnippets)
        {
            throw new ConfigurationValidationException(
                $"Configuration exceeds {MaximumCustomSnippets} custom snippets.");
        }

        try
        {
            var definitions = Snippets.Select(snippet =>
                    (snippet ?? throw new ArgumentException("Snippet entry must not be null."))
                    .ToDefinition())
                .ToArray();
            _ = new SnippetMatcher(definitions);
            return definitions;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            throw new ConfigurationValidationException(
                "Configuration contains an invalid snippet definition.",
                exception);
        }
    }
}

public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(string message)
        : base(message)
    {
    }

    public ConfigurationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
