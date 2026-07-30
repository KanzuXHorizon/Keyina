using Keyina.Host.Core.Translation;

namespace Keyina.Host.Tests;

internal static class TranslationTextProtectorTests
{
    [KeyinaTest("translation protector preserves technical tokens while allowing surrounding text to move")]
    private static void TechnicalTokensArePreserved()
    {
        const string source =
            "Fix getUserLocale() in apps/web for {user_name}, support@example.com and C:\\Temp\\config.json.";

        var protectedText = TranslationTextProtector.Protect(source);
        AssertEx.True(protectedText.UsesXmlTagHandling,
            "Technical text did not enable XML token protection.");
        AssertEx.True(protectedText.HasTranslatableContent,
            "Natural-language text was not detected around protected tokens.");
        AssertEx.True(
            protectedText.Payload.Contains("<keep id=\"0\">", StringComparison.Ordinal),
            "Protected payload did not contain deterministic keep tags.");

        var translatedPayload = protectedText.Payload
            .Replace("Fix ", "Repair ", StringComparison.Ordinal)
            .Replace(" in ", " inside ", StringComparison.Ordinal)
            .Replace(" for ", " for user ", StringComparison.Ordinal);
        var restored = protectedText.Restore(translatedPayload);

        AssertEx.True(
            restored.StartsWith("Repair getUserLocale()", StringComparison.Ordinal),
            "Natural-language text was not restored around the method token.");
        AssertEx.True(restored.Contains("apps/web", StringComparison.Ordinal),
            "Project path was not preserved.");
        AssertEx.True(restored.Contains("{user_name}", StringComparison.Ordinal),
            "Placeholder was not preserved.");
        AssertEx.True(restored.Contains("support@example.com", StringComparison.Ordinal),
            "Email address was not preserved.");
        AssertEx.True(restored.Contains("C:\\Temp\\config.json", StringComparison.Ordinal),
            "Windows path was not preserved.");
    }

    [KeyinaTest("translation protector skips network work for content made only of protected tokens")]
    private static void TokenOnlyContentHasNoTranslatableText()
    {
        var protectedText = TranslationTextProtector.Protect(
            "https://example.com ${user} getUserLocale()");

        AssertEx.True(protectedText.UsesXmlTagHandling,
            "Token-only content did not enable XML protection.");
        AssertEx.False(protectedText.HasTranslatableContent,
            "Token-only content was incorrectly considered translatable.");
        AssertEx.Equal(
            "https://example.com ${user} getUserLocale()",
            protectedText.Restore(protectedText.Payload));
    }

    [KeyinaTest("translation protector rejects missing duplicate and unknown keep tags")]
    private static void InvalidProtectedResponsesAreRejected()
    {
        var protectedText = TranslationTextProtector.Protect("Hello {name} at https://example.com");

        AssertInvalid(() => protectedText.Restore(
            "<root>Xin chào <keep id=\"0\">{name}</keep></root>"));
        AssertInvalid(() => protectedText.Restore(
            "<root><keep id=\"0\">{name}</keep><keep id=\"0\">{name}</keep><keep id=\"1\">url</keep></root>"));
        AssertInvalid(() => protectedText.Restore(
            "<root><keep id=\"0\">{name}</keep><keep id=\"1\">url</keep><keep id=\"7\">unknown</keep></root>"));
        AssertInvalid(() => protectedText.Restore(
            "<!DOCTYPE root [<!ENTITY injected \"translated\">]><root>&injected;<keep id=\"0\">{name}</keep><keep id=\"1\">url</keep></root>"));
    }

    private static void AssertInvalid(Action action)
    {
        try
        {
            action();
        }
        catch (TranslationException exception)
        {
            AssertEx.Equal(TranslationFailureCode.InvalidResponse, exception.FailureCode);
            return;
        }

        throw new InvalidOperationException("Expected protected translation response rejection.");
    }
}
