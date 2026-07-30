namespace Keyina.Host.Core.Translation;

public sealed record TranslationProviderPreferences(
    bool LibreTranslateEnabled,
    string LibreTranslateEndpoint,
    bool AllowLocalEndpoint)
{
    public const int MaximumEndpointLength = 2_048;

    public static TranslationProviderPreferences Default { get; } = new(
        LibreTranslateEnabled: false,
        LibreTranslateEndpoint: string.Empty,
        AllowLocalEndpoint: false);

    public TranslationProviderPreferences Normalize()
    {
        var endpoint = (LibreTranslateEndpoint ?? string.Empty).Trim();
        if (endpoint.Length > MaximumEndpointLength)
        {
            throw new ArgumentException(
                $"LibreTranslate endpoint exceeds {MaximumEndpointLength} characters.",
                nameof(LibreTranslateEndpoint));
        }
        if (LibreTranslateEnabled && endpoint.Length == 0)
        {
            throw new ArgumentException(
                "LibreTranslate endpoint is required when fallback is enabled.",
                nameof(LibreTranslateEndpoint));
        }
        if (endpoint.Length > 0 &&
            (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new ArgumentException(
                "LibreTranslate endpoint must be an absolute HTTP or HTTPS URL.",
                nameof(LibreTranslateEndpoint));
        }
        return this with { LibreTranslateEndpoint = endpoint };
    }

    public void Validate() => _ = Normalize();
}
