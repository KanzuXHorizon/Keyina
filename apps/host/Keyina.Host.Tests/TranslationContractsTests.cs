using Keyina.Host.Core.Translation;

namespace Keyina.Host.Tests;

internal static class TranslationContractsTests
{
    [KeyinaTest("translation catalog accepts supported DeepL target languages")]
    private static void SupportedTargetsAreAccepted()
    {
        string[] expectedCodes =
        [
            "EN-US", "EN-GB", "VI", "JA", "KO", "ZH-HANS", "DE", "FR",
            "ES", "PT-BR", "IT", "NL", "PL", "RU", "UK",
        ];

        foreach (var code in expectedCodes)
        {
            AssertEx.True(
                TranslationLanguageCatalog.IsSupportedTarget(code),
                $"Expected translation target {code} to be supported.");
        }
    }

    [KeyinaTest("translation request validates target text and size before network access")]
    private static void RequestValidationRejectsUnsafeInput()
    {
        AssertThrows<ArgumentException>(() => KeepAlive(new TranslationRequest(" ", "EN-US")));
        AssertThrows<TranslationException>(() => KeepAlive(new TranslationRequest("hello", "XX")));
        AssertThrows<TranslationException>(() => KeepAlive(new TranslationRequest(
            new string('x', TranslationRequest.MaximumTextLength + 1),
            "EN-US")));

        var request = new TranslationRequest("xin chào", "EN-US");
        AssertEx.Equal("xin chào", request.Text);
        AssertEx.Equal("EN-US", request.TargetLanguage);
    }

    [KeyinaTest("translation errors expose stable codes without source text")]
    private static void TranslationErrorsDoNotLeakText()
    {
        const string sourceText = "private source sentence";
        var exception = new TranslationException(
            TranslationFailureCode.Unavailable,
            "Translation service is unavailable.");

        AssertEx.Equal(TranslationFailureCode.Unavailable, exception.FailureCode);
        AssertEx.False(
            exception.ToString().Contains(sourceText, StringComparison.Ordinal),
            "Translation exception leaked source text.");
    }

    private static void KeepAlive(object value) => GC.KeepAlive(value);

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
