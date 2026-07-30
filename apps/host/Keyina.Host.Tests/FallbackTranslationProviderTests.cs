using Keyina.Host.Core.Translation;
using Keyina.Host.Translation;

namespace Keyina.Host.Tests;

internal static class FallbackTranslationProviderTests
{
    [KeyinaTest("translation fallback uses LibreTranslate only for unavailable rate and quota failures")]
    private static void FallsBackOnlyForTransientPrimaryFailures()
    {
        foreach (var failure in new[]
                 {
                     TranslationFailureCode.Unavailable,
                     TranslationFailureCode.RateLimited,
                     TranslationFailureCode.QuotaExceeded,
                 })
        {
            var primary = new ThrowingProvider(failure);
            var fallback = new RecordingProvider(
                new TranslationResult("Xin chào", "EN", "LibreTranslate"));
            var provider = new FallbackTranslationProvider(
                primary,
                fallback,
                () => true,
                () => "fallback-key");

            var result = provider.TranslateAsync(
                    "deepl-key:fx",
                    new TranslationRequest("Hello", "VI"),
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            AssertEx.Equal("Xin chào", result.Text);
            AssertEx.Equal(1, primary.CallCount);
            AssertEx.Equal(1, fallback.CallCount);
            AssertEx.Equal("fallback-key", fallback.LastApiKey);
        }
    }

    [KeyinaTest("translation fallback never hides authentication or protected-token failures")]
    private static void DoesNotFallbackForPermanentFailures()
    {
        foreach (var failure in new[]
                 {
                     TranslationFailureCode.AuthenticationFailed,
                     TranslationFailureCode.InvalidResponse,
                     TranslationFailureCode.UnsupportedLanguage,
                     TranslationFailureCode.SelectionTooLarge,
                 })
        {
            var fallback = new RecordingProvider(
                new TranslationResult("unused", "EN", "LibreTranslate"));
            var provider = new FallbackTranslationProvider(
                new ThrowingProvider(failure),
                fallback,
                () => true,
                () => "fallback-key");

            var exception = AssertThrows<TranslationException>(() =>
                provider.TranslateAsync(
                        "deepl-key:fx",
                        new TranslationRequest("Hello", "VI"),
                        CancellationToken.None)
                    .GetAwaiter().GetResult());

            AssertEx.Equal(failure, exception.FailureCode);
            AssertEx.Equal(0, fallback.CallCount);
        }
    }

    [KeyinaTest("translation fallback supports LibreTranslate-only configuration without a DeepL key")]
    private static void SupportsFallbackOnlyConfiguration()
    {
        var primary = new RecordingProvider(
            new TranslationResult("unused", "EN", "DeepL"));
        var fallback = new RecordingProvider(
            new TranslationResult("Xin chào", "EN", "LibreTranslate"));
        var provider = new FallbackTranslationProvider(
            primary,
            fallback,
            () => true,
            () => null);

        var result = provider.TranslateAsync(
                string.Empty,
                new TranslationRequest("Hello", "VI"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal("Xin chào", result.Text);
        AssertEx.Equal(0, primary.CallCount);
        AssertEx.Equal(1, fallback.CallCount);
        AssertEx.Equal(string.Empty, fallback.LastApiKey);
    }

    [KeyinaTest("translation fallback remains disabled unless explicitly configured")]
    private static void DisabledFallbackReturnsPrimaryFailure()
    {
        var fallback = new RecordingProvider(
            new TranslationResult("unused", "EN", "LibreTranslate"));
        var provider = new FallbackTranslationProvider(
            new ThrowingProvider(TranslationFailureCode.QuotaExceeded),
            fallback,
            () => false,
            () => "fallback-key");

        var exception = AssertThrows<TranslationException>(() =>
            provider.TranslateAsync(
                    "deepl-key:fx",
                    new TranslationRequest("Hello", "VI"),
                    CancellationToken.None)
                .GetAwaiter().GetResult());

        AssertEx.Equal(TranslationFailureCode.QuotaExceeded, exception.FailureCode);
        AssertEx.Equal(0, fallback.CallCount);
    }

    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class ThrowingProvider(TranslationFailureCode failureCode)
        : ITranslationProvider
    {
        public int CallCount { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<TranslationResult>(new TranslationException(
                failureCode,
                "provider failed"));
        }
    }

    private sealed class RecordingProvider(TranslationResult result)
        : ITranslationProvider
    {
        public int CallCount { get; private set; }

        public string? LastApiKey { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastApiKey = apiKey;
            return Task.FromResult(result);
        }
    }
}
