namespace Keyina.Host.Core.Speech;

public sealed record SpeechLanguage(string Code, string DisplayName);

public static class SpeechLanguageCatalog
{
    private static readonly SpeechLanguage[] Languages =
    [
        new("auto", "Tự động nhận diện"),
        new("vi", "Tiếng Việt"),
        new("en", "English"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("cmn", "中文（普通话）"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("pt", "Português"),
        new("it", "Italiano"),
        new("th", "ภาษาไทย"),
        new("id", "Bahasa Indonesia"),
        new("ru", "Русский"),
    ];

    private static readonly Dictionary<string, SpeechLanguage> ByCode =
        Languages.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SpeechLanguage> Supported => Languages;

    public static string Normalize(string? code)
    {
        var candidate = string.IsNullOrWhiteSpace(code) ? "auto" : code.Trim();
        if (!ByCode.TryGetValue(candidate, out var language))
        {
            throw new ArgumentException("The selected speech language is not supported.", nameof(code));
        }

        return language.Code;
    }

    public static string GetDisplayName(string? code)
    {
        var normalized = Normalize(code);
        return ByCode[normalized].DisplayName;
    }
}
