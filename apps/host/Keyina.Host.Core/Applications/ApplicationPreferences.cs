namespace Keyina.Host.Core.Applications;

public enum ApplicationFeature
{
    VietnameseTyping,
    Speech,
    Translation,
    VisualFeedback,
}

public sealed record ApplicationPreferences(
    string[] DisableVietnamese,
    string[] DisableSpeech,
    string[] DisableTranslation,
    string[] SuppressVisualFeedback)
{
    public const int MaximumEntriesPerFeature = 256;
    public const int MaximumExecutableNameLength = 260;

    public static ApplicationPreferences Default { get; } = new([], [], [], []);

    public ApplicationPreferences Normalize() => new(
        NormalizeList(DisableVietnamese, nameof(DisableVietnamese)),
        NormalizeList(DisableSpeech, nameof(DisableSpeech)),
        NormalizeList(DisableTranslation, nameof(DisableTranslation)),
        NormalizeList(SuppressVisualFeedback, nameof(SuppressVisualFeedback)));

    public void Validate() => _ = Normalize();

    public bool IsDisabled(
        ApplicationFeature feature,
        string? executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return false;
        }

        var normalized = NormalizeExecutableName(executableName);
        var entries = feature switch
        {
            ApplicationFeature.VietnameseTyping => DisableVietnamese,
            ApplicationFeature.Speech => DisableSpeech,
            ApplicationFeature.Translation => DisableTranslation,
            ApplicationFeature.VisualFeedback => SuppressVisualFeedback,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };
        return entries.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeExecutableName(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        var trimmed = executableName.Trim();
        if (trimmed.Length > MaximumExecutableNameLength)
        {
            throw new ArgumentException(
                $"Executable name exceeds {MaximumExecutableNameLength} characters.",
                nameof(executableName));
        }
        if (trimmed.Any(char.IsControl) ||
            trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Contains('/', StringComparison.Ordinal) ||
            trimmed.Contains('\\', StringComparison.Ordinal) ||
            trimmed.Contains(':', StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetFileName(trimmed),
                trimmed,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Application rules accept executable file names only, without paths or wildcards.",
                nameof(executableName));
        }
        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Length <= 4)
        {
            throw new ArgumentException(
                "Application rules require a Windows executable name ending in .exe.",
                nameof(executableName));
        }
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "Application rule contains invalid file-name characters.",
                nameof(executableName));
        }
        return trimmed.ToLowerInvariant();
    }

    private static string[] NormalizeList(
        string[]? values,
        string parameterName)
    {
        if (values is null)
        {
            throw new ArgumentException(
                "Application rule list must not be null.",
                parameterName);
        }
        if (values.Length > MaximumEntriesPerFeature)
        {
            throw new ArgumentException(
                $"Application rule list exceeds {MaximumEntriesPerFeature} entries.",
                parameterName);
        }

        var normalized = values
            .Select(NormalizeExecutableName)
            .ToArray();
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException(
                "Application rule list contains duplicate executable names.",
                parameterName);
        }
        return normalized;
    }
}
