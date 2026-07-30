using System.Net;
using System.Text;
using System.Text.Json;
using Keyina.Host.Core.Translation;
using Keyina.Host.Translation;

namespace Keyina.Host.Tests;

internal static class DeepLTranslationProviderTests
{
    [KeyinaTest("DeepL free keys use the free endpoint and documented request contract")]
    private static void FreeEndpointAndRequestContract()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"translations\":[{\"detected_source_language\":\"VI\",\"text\":\"Hello\"}]}"));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);

        var result = provider.TranslateAsync(
                "test-key:fx",
                new TranslationRequest("Xin chào", "EN-US"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.NotNull(handler.Request, "DeepL request was not sent.");
        AssertEx.Equal(HttpMethod.Post, handler.Request!.Method);
        AssertEx.Equal("https://api-free.deepl.com/v2/translate", handler.Request.RequestUri!.AbsoluteUri);
        AssertEx.Equal("DeepL-Auth-Key", handler.Request.Headers.Authorization!.Scheme);
        AssertEx.Equal("test-key:fx", handler.Request.Headers.Authorization.Parameter);
        AssertEx.True(
            handler.RequestBody!.Contains("Xin chào", StringComparison.Ordinal),
            "DeepL request unnecessarily escaped UTF-8 source text.");
        using var requestJson = JsonDocument.Parse(handler.RequestBody);
        var root = requestJson.RootElement;
        AssertEx.Equal("Xin chào", root.GetProperty("text")[0].GetString());
        AssertEx.Equal("EN-US", root.GetProperty("target_lang").GetString());
        AssertEx.Equal(
            "prefer_quality_optimized",
            root.GetProperty("model_type").GetString());
        AssertEx.True(root.GetProperty("preserve_formatting").GetBoolean(),
            "DeepL request did not preserve the selected layout.");
        AssertEx.Equal("Hello", result.Text);
        AssertEx.Equal("VI", result.DetectedSourceLanguage);
        AssertEx.Equal("DeepL", result.Provider);
    }

    [KeyinaTest("DeepL protects technical tokens with v2 XML handling and restores exact values")]
    private static void TechnicalTokensUseXmlProtection()
    {
        const string protectedResponse =
            "<root>Xin chào <keep id=\"0\">{name}</keep> tại <keep id=\"1\">https://example.com</keep></root>";
        var handler = new RecordingHandler(_ => JsonResponse(
            JsonSerializer.Serialize(new
            {
                translations = new[]
                {
                    new
                    {
                        detected_source_language = "EN",
                        text = protectedResponse,
                    },
                },
            })));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);

        var result = provider.TranslateAsync(
                "test-key:fx",
                new TranslationRequest("Hello {name} at https://example.com", "VI"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        using var requestJson = JsonDocument.Parse(handler.RequestBody!);
        var root = requestJson.RootElement;
        AssertEx.Equal("xml", root.GetProperty("tag_handling").GetString());
        AssertEx.Equal("v2", root.GetProperty("tag_handling_version").GetString());
        AssertEx.Equal("keep", root.GetProperty("ignore_tags")[0].GetString());
        AssertEx.Equal("keep", root.GetProperty("non_splitting_tags")[0].GetString());
        AssertEx.Equal("nonewlines", root.GetProperty("split_sentences").GetString());
        AssertEx.Equal("Xin chào {name} tại https://example.com", result.Text);
    }

    [KeyinaTest("DeepL avoids quota use for content containing only protected technical tokens")]
    private static void TokenOnlyContentSkipsNetwork()
    {
        var handler = new RecordingHandler(_ =>
            throw new InvalidOperationException("Token-only translation reached the network."));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);

        var result = provider.TranslateAsync(
                "test-key:fx",
                new TranslationRequest("https://example.com ${user}", "VI"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(handler.Request is null,
            "Token-only translation unexpectedly sent an HTTP request.");
        AssertEx.Equal("https://example.com ${user}", result.Text);
    }

    [KeyinaTest("DeepL rejects oversized protected requests before network access")]
    private static void OversizedProtectedRequestIsRejectedLocally()
    {
        var handler = new RecordingHandler(_ =>
            throw new InvalidOperationException("Oversized translation reached the network."));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);
        var tokenHeavyText = "Translate " +
            string.Concat(Enumerable.Repeat("%s ", 6_000));

        var exception = AssertThrows<TranslationException>(() =>
            provider.TranslateAsync(
                    "test-key:fx",
                    new TranslationRequest(tokenHeavyText, "VI"),
                    CancellationToken.None)
                .GetAwaiter().GetResult());

        AssertEx.Equal(TranslationFailureCode.SelectionTooLarge, exception.FailureCode);
        AssertEx.True(handler.Request is null,
            "Oversized protected request unexpectedly sent an HTTP request.");
    }

    [KeyinaTest("DeepL rejects responses that alter protected technical tokens")]
    private static void AlteredProtectedTokensAreRejected()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"translations\":[{\"detected_source_language\":\"EN\",\"text\":\"<root>Xin chào</root>\"}]}"));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);

        var exception = AssertThrows<TranslationException>(() =>
            provider.TranslateAsync(
                    "test-key:fx",
                    new TranslationRequest("Hello {name}", "VI"),
                    CancellationToken.None)
                .GetAwaiter().GetResult());

        AssertEx.Equal(TranslationFailureCode.InvalidResponse, exception.FailureCode);
    }

    [KeyinaTest("DeepL non free keys use the production endpoint")]
    private static void ProEndpointIsSelected()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"translations\":[{\"detected_source_language\":\"EN\",\"text\":\"Bonjour\"}]}"));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);

        _ = provider.TranslateAsync(
                "pro-key",
                new TranslationRequest("Hello", "FR"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal("https://api.deepl.com/v2/translate", handler.Request!.RequestUri!.AbsoluteUri);
    }

    [KeyinaTest("DeepL maps authentication rate and quota failures to stable codes")]
    private static void HttpFailuresAreMapped()
    {
        var cases = new[]
        {
            (HttpStatusCode.Forbidden, TranslationFailureCode.AuthenticationFailed),
            ((HttpStatusCode)429, TranslationFailureCode.RateLimited),
            ((HttpStatusCode)456, TranslationFailureCode.QuotaExceeded),
        };

        foreach (var (statusCode, expectedCode) in cases)
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
            using var client = new HttpClient(handler);
            var provider = new DeepLTranslationProvider(client);

            var exception = AssertThrows<TranslationException>(() =>
                provider.TranslateAsync(
                        "test-key:fx",
                        new TranslationRequest("hello", "VI"),
                        CancellationToken.None)
                    .GetAwaiter().GetResult());

            AssertEx.Equal(expectedCode, exception.FailureCode);
        }
    }

    [KeyinaTest("DeepL rejects malformed and empty translation responses")]
    private static void InvalidResponsesAreRejected()
    {
        string[] invalidBodies =
        [
            "not-json",
            "{}",
            "{\"translations\":[]}",
            "{\"translations\":[{\"detected_source_language\":\"VI\",\"text\":\"\"}]}",
        ];

        foreach (var body in invalidBodies)
        {
            var handler = new RecordingHandler(_ => JsonResponse(body));
            using var client = new HttpClient(handler);
            var provider = new DeepLTranslationProvider(client);

            var exception = AssertThrows<TranslationException>(() =>
                provider.TranslateAsync(
                        "test-key:fx",
                        new TranslationRequest("hello", "VI"),
                        CancellationToken.None)
                    .GetAwaiter().GetResult());

            AssertEx.Equal(TranslationFailureCode.InvalidResponse, exception.FailureCode);
        }
    }

    [KeyinaTest("DeepL network failures do not expose selected text")]
    private static void NetworkFailuresDoNotLeakText()
    {
        const string selectedText = "secret selected text";
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        var provider = new DeepLTranslationProvider(client);

        var exception = AssertThrows<TranslationException>(() =>
            provider.TranslateAsync(
                    "test-key:fx",
                    new TranslationRequest(selectedText, "VI"),
                    CancellationToken.None)
                .GetAwaiter().GetResult());

        AssertEx.Equal(TranslationFailureCode.Unavailable, exception.FailureCode);
        AssertEx.False(
            exception.ToString().Contains(selectedText, StringComparison.Ordinal),
            "Provider exception leaked selected text.");
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static TException AssertThrows<TException>(Action action) where TException : Exception
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

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content?.ReadAsStringAsync(cancellationToken)
                .GetAwaiter().GetResult();
            return Task.FromResult(responseFactory(request));
        }
    }
}
