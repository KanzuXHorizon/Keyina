using Keyina.Host.Core.Translation;

namespace Keyina.Host.Translation;

public sealed class FallbackTranslationProvider : ITranslationProvider
{
    private readonly ITranslationProvider primary;
    private readonly ITranslationProvider fallback;
    private readonly Func<bool> fallbackEnabled;
    private readonly Func<string?> fallbackApiKey;

    public FallbackTranslationProvider(
        ITranslationProvider primary,
        ITranslationProvider fallback,
        Func<bool> fallbackEnabled,
        Func<string?> fallbackApiKey)
    {
        this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
        this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        this.fallbackEnabled = fallbackEnabled ??
            throw new ArgumentNullException(nameof(fallbackEnabled));
        this.fallbackApiKey = fallbackApiKey ??
            throw new ArgumentNullException(nameof(fallbackApiKey));
    }

    public async Task<TranslationResult> TranslateAsync(
        string apiKey,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(apiKey) && fallbackEnabled())
        {
            return await fallback.TranslateAsync(
                    fallbackApiKey() ?? string.Empty,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        try
        {
            return await primary.TranslateAsync(apiKey, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TranslationException exception) when (
            ShouldFallback(exception.FailureCode) && fallbackEnabled())
        {
            return await fallback.TranslateAsync(
                    fallbackApiKey() ?? string.Empty,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool ShouldFallback(TranslationFailureCode failureCode) =>
        failureCode is TranslationFailureCode.Unavailable or
            TranslationFailureCode.RateLimited or
            TranslationFailureCode.QuotaExceeded;
}
