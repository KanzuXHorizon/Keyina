namespace Keyina.Host.Core.Translation;

public enum TranslationFailureCode
{
    Disabled,
    CredentialMissing,
    NoSelection,
    FocusChanged,
    AuthenticationFailed,
    RateLimited,
    QuotaExceeded,
    Unavailable,
    InvalidResponse,
    UnsupportedLanguage,
    SelectionTooLarge,
}

public sealed class TranslationException : Exception
{
    public TranslationException(
        TranslationFailureCode failureCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureCode = failureCode;
    }

    public TranslationFailureCode FailureCode { get; }
}

public sealed record TranslationLanguage(string Code, string DisplayName);

public static class TranslationLanguageCatalog
{
    private static readonly TranslationLanguage[] Languages =
    [
        new("EN-US", "English (United States)"),
        new("EN-GB", "English (United Kingdom)"),
        new("VI", "Tiếng Việt"),
        new("JA", "日本語"),
        new("KO", "한국어"),
        new("ZH-HANS", "简体中文"),
        new("DE", "Deutsch"),
        new("FR", "Français"),
        new("ES", "Español"),
        new("PT-BR", "Português (Brasil)"),
        new("IT", "Italiano"),
        new("NL", "Nederlands"),
        new("PL", "Polski"),
        new("RU", "Русский"),
        new("UK", "Українська"),
    ];

    private static readonly Dictionary<string, TranslationLanguage> ByCode =
        Languages.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TranslationLanguage> SupportedTargets => Languages;

    public static bool IsSupportedTarget(string? code) =>
        !string.IsNullOrWhiteSpace(code) && ByCode.ContainsKey(code);

    public static string NormalizeTarget(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!ByCode.TryGetValue(code, out var language))
        {
            throw new TranslationException(
                TranslationFailureCode.UnsupportedLanguage,
                "The selected translation language is not supported.");
        }
        return language.Code;
    }

    public static string GetDisplayName(string code) =>
        ByCode.TryGetValue(code, out var language)
            ? language.DisplayName
            : code;
}

public sealed record TranslationRequest
{
    public const int MaximumTextLength = 20_000;

    public TranslationRequest(string text, string targetLanguage, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaximumTextLength)
        {
            throw new TranslationException(
                TranslationFailureCode.SelectionTooLarge,
                $"Selected text exceeds the {MaximumTextLength}-character translation limit.");
        }

        Text = text;
        TargetLanguage = TranslationLanguageCatalog.NormalizeTarget(targetLanguage);
        Context = string.IsNullOrWhiteSpace(context) ? null : context;
    }

    public string Text { get; }

    public string TargetLanguage { get; }

    public string? Context { get; }
}

public sealed record TranslationResult(
    string Text,
    string DetectedSourceLanguage,
    string Provider);

public interface ITranslationProvider
{
    Task<TranslationResult> TranslateAsync(
        string apiKey,
        TranslationRequest request,
        CancellationToken cancellationToken);
}
