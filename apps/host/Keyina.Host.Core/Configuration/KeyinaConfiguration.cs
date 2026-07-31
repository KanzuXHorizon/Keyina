using Keyina.Host.Core.Applications;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Snippets;
using Keyina.Host.Core.Translation;
using System.Text.Json;

namespace Keyina.Host.Core.Configuration;

public enum KeyinaTheme
{
    System,
    Light,
    Dark,
}

public sealed record SnippetExecutionConfiguration(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    int TimeoutMilliseconds)
{
    public const int MinimumTimeoutMilliseconds = 250;
    public const int MaximumTimeoutMilliseconds = 10_000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath) ||
            !Path.IsPathFullyQualified(ExecutablePath) ||
            !string.Equals(Path.GetExtension(ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Snippet executable path must be an absolute .exe path.", nameof(ExecutablePath));
        }
        if (!string.IsNullOrWhiteSpace(WorkingDirectory) &&
            !Path.IsPathFullyQualified(WorkingDirectory))
        {
            throw new ArgumentException("Snippet working directory must be absolute.", nameof(WorkingDirectory));
        }
        if (TimeoutMilliseconds is < MinimumTimeoutMilliseconds or > MaximumTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutMilliseconds));
        }
        if ((Arguments?.Length ?? 0) > 8_192 || (WorkingDirectory?.Length ?? 0) > 1_024)
        {
            throw new ArgumentException("Snippet command fields are too long.");
        }
    }
}

public sealed record SnippetConfiguration(
    string Trigger,
    string Expansion,
    bool CaseSensitive,
    bool PreserveDelimiter,
    string Delimiters,
    string[] AllowedApplications,
    string[] ExcludedApplications,
    SnippetExecutionConfiguration? Execution = null)
{
    public SnippetDefinition ToDefinition()
    {
        if (Delimiters is null)
        {
            throw new ArgumentException("Snippet delimiters must not be null.", nameof(Delimiters));
        }

        Execution?.Validate();
        if (Execution is not null && PreserveDelimiter)
        {
            throw new ArgumentException(
                "Command-output snippets cannot preserve the delimiter because output is inserted asynchronously.",
                nameof(PreserveDelimiter));
        }
        var expansion = Execution is null
            ? Expansion
            : JsonSerializer.Serialize(Execution);
        return new SnippetDefinition(
            Trigger,
            expansion,
            CaseSensitive,
            PreserveDelimiter,
            Delimiters.ToHashSet(),
            (AllowedApplications ?? throw new ArgumentException(
                "Allowed application list must not be null.",
                nameof(AllowedApplications))).ToHashSet(StringComparer.OrdinalIgnoreCase),
            (ExcludedApplications ?? throw new ArgumentException(
                "Excluded application list must not be null.",
                nameof(ExcludedApplications))).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Execution is null ? SnippetCommand.None : SnippetCommand.ExternalOutput);
    }
}

public sealed record KeyinaConfiguration(
    int SchemaVersion,
    bool VietnameseEnabled,
    bool SpeechEnabled,
    KeyinaTheme Theme,
    SnippetConfiguration[] Snippets,
    FeedbackPreferences? Feedback = null)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumCustomSnippets = 10_000;

    public bool TranslationEnabled { get; init; }

    public bool TranslationPreviewEnabled { get; init; }

    public string TranslationTargetLanguage { get; init; } = "VI";

    public TranslationProviderPreferences TranslationProviders { get; init; } =
        TranslationProviderPreferences.Default;

    public HotkeyPreferences Hotkeys { get; init; } = HotkeyPreferences.Default;

    public bool? FirstRunCompleted { get; init; }

    public ApplicationPreferences Applications { get; init; } = ApplicationPreferences.Default;

    public static KeyinaConfiguration Default { get; } = new(
        CurrentSchemaVersion,
        VietnameseEnabled: true,
        SpeechEnabled: false,
        KeyinaTheme.System,
        [],
        FeedbackPreferences.Default)
    {
        FirstRunCompleted = false,
    };

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
        if (Feedback is null || !Enum.IsDefined(Feedback.Mode))
        {
            throw new ConfigurationValidationException("Configuration feedback mode is invalid.");
        }
        if (!TranslationLanguageCatalog.IsSupportedTarget(TranslationTargetLanguage))
        {
            throw new ConfigurationValidationException(
                "Configuration translation target language is invalid.");
        }
        try
        {
            (TranslationProviders ?? throw new ArgumentException(
                "Translation provider preferences must not be null.",
                nameof(TranslationProviders))).Validate();
        }
        catch (ArgumentException exception)
        {
            throw new ConfigurationValidationException(
                "Configuration contains invalid translation provider preferences.",
                exception);
        }
        try
        {
            (Hotkeys ?? throw new ArgumentException(
                "Hotkey preferences must not be null.",
                nameof(Hotkeys))).Validate();
        }
        catch (ArgumentException exception)
        {
            throw new ConfigurationValidationException(
                "Configuration contains invalid hotkey preferences.",
                exception);
        }
        if (FirstRunCompleted is null)
        {
            throw new ConfigurationValidationException(
                "Configuration first-run state is missing.");
        }
        try
        {
            (Applications ?? throw new ArgumentException(
                "Application preferences must not be null.",
                nameof(Applications))).Validate();
        }
        catch (ArgumentException exception)
        {
            throw new ConfigurationValidationException(
                "Configuration contains invalid application preferences.",
                exception);
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
